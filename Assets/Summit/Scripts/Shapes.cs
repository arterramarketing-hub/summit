using System.Collections.Generic;
using UnityEngine;

namespace Summit
{
    /// <summary>
    /// Small meshes built in code for the pickups: a can with a label that wraps all the way round,
    /// its lids, a spring coil, a faceted star and an up arrow.
    /// </summary>
    public static class Shapes
    {
        /// <summary>The side of a cylinder from y=0 to y=height; u runs once round it, v from bottom to top.</summary>
        public static Mesh Tube(float r, float height, int seg, bool uv)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                // u grows to the right as you look at the can from outside, so the label reads the right way round
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * r); v.Add(d * r + Vector3.up * height);
                n.Add(d); n.Add(d);
                t.Add(new Vector2(i / (float)seg, 0f)); t.Add(new Vector2(i / (float)seg, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                Face(v, tri, b0, t0, t1, n[b0]); Face(v, tri, b0, t1, b1, n[b0]);
            }
            return Make("Can", v, n, t, tri);
        }

        /// <summary>A flat disc facing +Y, with its texture laid straight across it.</summary>
        public static Mesh Disc(float r, int seg)
        {
            var v = new List<Vector3> { Vector3.zero }; var n = new List<Vector3> { Vector3.up }; var t = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r); n.Add(Vector3.up);
                t.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
            }
            for (int i = 1; i <= seg; i++) Face(v, tri, 0, i, i + 1, Vector3.up);
            return Make("Lid", v, n, t, tri);
        }

        /// <summary>A coil spring standing on y=0: coil radius R, wire radius r, turns, height.</summary>
        public static Mesh Coil(float R, float r, float turns, float height, int perTurn, int sides)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<Vector2>(); var tri = new List<int>();
            int steps = Mathf.CeilToInt(turns * perTurn);
            for (int s = 0; s <= steps; s++)
            {
                float k = s / (float)steps, a = k * turns * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var c = radial * R + Vector3.up * (height * k);
                var tangent = new Vector3(-Mathf.Sin(a) * R * turns * Mathf.PI * 2f, height, Mathf.Cos(a) * R * turns * Mathf.PI * 2f).normalized;
                var b = Vector3.Cross(tangent, radial).normalized;
                for (int j = 0; j <= sides; j++)
                {
                    float q = j / (float)sides * Mathf.PI * 2f;
                    var d = radial * Mathf.Cos(q) + b * Mathf.Sin(q);
                    v.Add(c + d * r); n.Add(d); t.Add(new Vector2(k, j / (float)sides));
                }
            }
            int row = sides + 1;
            for (int s = 0; s < steps; s++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = s * row + j, a1 = a0 + 1, b0 = a0 + row, b1 = b0 + 1;
                    Face(v, tri, a0, b0, b1, n[a0] + n[b1]); Face(v, tri, a0, b1, a1, n[a0] + n[b1]);
                }
            return Make("Coil", v, n, t, tri);
        }

        /// <summary>A five-pointed star standing upright (in XY), faceted like a cut gem so it flashes as it turns.</summary>
        public static Mesh Star(float outer, float inner, float depth)
        {
            var ring = new Vector3[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                ring[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (i % 2 == 0 ? outer : inner);
            }
            var v = new List<Vector3>(); var tri = new List<int>();
            foreach (float side in new[] { 1f, -1f })
            {
                var tip = new Vector3(0f, 0f, depth * side);
                for (int i = 0; i < 10; i++) Flat(v, tri, tip, ring[i], ring[(i + 1) % 10], new Vector3(0f, 0f, side));
            }
            return MakeFlat("Star", v, tri);
        }

        /// <summary>A chunky up arrow standing in XY with its base at y=0: head width w, height h, stem width s, depth d.</summary>
        public static Mesh Chevron(float w, float h, float s, float d)
        {
            var v = new List<Vector3>(); var tri = new List<int>();
            Extrude(v, tri, new[] { new Vector2(-w * 0.5f, h * 0.45f), new Vector2(0f, h), new Vector2(w * 0.5f, h * 0.45f) }, d);
            Extrude(v, tri, new[] { new Vector2(-s * 0.5f, 0f), new Vector2(-s * 0.5f, h * 0.46f), new Vector2(s * 0.5f, h * 0.46f), new Vector2(s * 0.5f, 0f) }, d);
            return MakeFlat("Arrow", v, tri);
        }

        // a convex outline pushed out to a slab, front at +z and back at -z
        static void Extrude(List<Vector3> v, List<int> tri, Vector2[] pts, float d)
        {
            int k = pts.Length;
            for (int i = 1; i < k - 1; i++)
            {
                Flat(v, tri, P(pts[0], d), P(pts[i], d), P(pts[i + 1], d), Vector3.forward);
                Flat(v, tri, P(pts[0], -d), P(pts[i], -d), P(pts[i + 1], -d), Vector3.back);
            }
            Vector2 mid = Vector2.zero; foreach (var p in pts) mid += p; mid /= k;
            for (int i = 0; i < k; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % k];
                Vector2 e = ((a + b) * 0.5f - mid);
                var outN = new Vector3(e.x, e.y, 0f);
                Flat(v, tri, P(a, d), P(b, d), P(b, -d), outN);
                Flat(v, tri, P(a, d), P(b, -d), P(a, -d), outN);
            }
        }

        static Vector3 P(Vector2 p, float d) => new Vector3(p.x, p.y, d * 0.5f);

        // Unity draws the side of a triangle whose normal is cross(b - a, c - a); wind each one so that
        // faces the way it should, instead of working the order out by hand for every shape
        static void Face(List<Vector3> v, List<int> tri, int a, int b, int c, Vector3 want)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), want) < 0f) { int s = b; b = c; c = s; }
            tri.Add(a); tri.Add(b); tri.Add(c);
        }

        static void Flat(List<Vector3> v, List<int> tri, Vector3 a, Vector3 b, Vector3 c, Vector3 want)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), want) < 0f) { var s = b; b = c; c = s; }
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
        }

        static Mesh Make(string name, List<Vector3> v, List<Vector3> n, List<Vector2> t, List<int> tri)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, t); m.SetTriangles(tri, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        // every triangle its own three vertices: hard facets
        static Mesh MakeFlat(string name, List<Vector3> v, List<int> tri)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
