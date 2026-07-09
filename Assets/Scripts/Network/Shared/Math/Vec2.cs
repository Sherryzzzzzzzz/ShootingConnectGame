// 2D向量
using System;

namespace ShootingGame.Shared.Math
{
    public struct Vec2 : IEquatable<Vec2>
    {
        public float x;
        public float y;

        public Vec2(float x, float y) { this.x = x; this.y = y; }

        public static readonly Vec2 Zero = new Vec2(0, 0);
        public static readonly Vec2 One = new Vec2(1, 1);

        public float Magnitude => GameMath.Sqrt(x * x + y * y);
        public float SqrMagnitude => x * x + y * y;

        public Vec2 Normalized
        {
            get
            {
                float mag = Magnitude;
                if (mag < 1e-6f) return Zero;
                return new Vec2(x / mag, y / mag);
            }
        }

        public static Vec2 ClampMagnitude(Vec2 v, float maxLength)
        {
            float sqr = v.SqrMagnitude;
            if (sqr > maxLength * maxLength)
            {
                float mag = GameMath.Sqrt(sqr);
                return new Vec2(v.x / mag * maxLength, v.y / mag * maxLength);
            }
            return v;
        }

        public static float Dot(Vec2 a, Vec2 b) => a.x * b.x + a.y * b.y;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Magnitude;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + (b - a) * GameMath.Clamp01(t);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.y + b.y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.x - b.x, a.y - b.y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.x, -a.y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.x * s, a.y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.x / s, a.y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public bool Equals(Vec2 other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 16);
        public override string ToString() => $"({x:F3}, {y:F3})";
    }
}