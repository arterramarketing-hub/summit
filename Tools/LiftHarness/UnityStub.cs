// A minimal stand-in for the parts of UnityEngine the mountain and the lift use: enough to compile them and run the maths.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public static class Mathf
    {
        public const float PI = (float)Math.PI, Deg2Rad = PI / 180f, Rad2Deg = 180f / PI;
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Asin(float f) => (float)Math.Asin(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Exp(float f) => (float)Math.Exp(f);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Abs(float f) => Math.Abs(f);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
        public static float Floor(float f) => (float)Math.Floor(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); }
        public static float Repeat(float t, float length) => Clamp(t - Floor(t / length) * length, 0f, length);
        public static float MoveTowards(float a, float b, float d) => Abs(b - a) <= d ? b : a + Sign(b - a) * d;
        public static float PerlinNoise(float x, float y) => 0.5f;
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public float magnitude => Mathf.Sqrt(x * x + y * y); }
    public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0); public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0); public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1); public static Vector3 back => new Vector3(0, 0, -1); public static Vector3 right => new Vector3(1, 0, 0);
        public float magnitude => Mathf.Sqrt(x * x + y * y + z * z); public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * Mathf.Clamp01(t);
        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }
    public static class Random { static System.Random r = new System.Random(1); public static int Range(int a, int b) => r.Next(a, b); public static float Range(float a, float b) => a + (b - a) * (float)r.NextDouble(); public static float value => (float)r.NextDouble(); }
}
namespace Summit
{
    public static class Camp { public static bool InCamp(float x, float z) => false; }   // (the camp is behind the start: no lift goes there)
}
