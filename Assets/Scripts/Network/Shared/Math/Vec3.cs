// 3D向量
using System;

namespace ShootingGame.Shared.Math
{
    public struct Vec3 : IEquatable<Vec3>
    {
        public float x;
        public float y;
        public float z;

        public Vec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 One = new Vec3(1, 1, 1);
        public static readonly Vec3 Up = new Vec3(0, 1, 0);
        public static readonly Vec3 Down = new Vec3(0, -1, 0);
        public static readonly Vec3 Forward = new Vec3(0, 0, 1);
        public static readonly Vec3 Back = new Vec3(0, 0, -1);
        public static readonly Vec3 Right = new Vec3(1, 0, 0);
        public static readonly Vec3 Left = new Vec3(-1, 0, 0);

        public float Magnitude => GameMath.Sqrt(x * x + y * y + z * z);
        public float SqrMagnitude => x * x + y * y + z * z;

        public Vec3 Normalized
        {
            get
            {
                float mag = Magnitude;
                if (mag < 1e-6f) return Zero;
                return new Vec3(x / mag, y / mag, z / mag);
            }
        }

        public static Vec3 ClampMagnitude(Vec3 v, float maxLength)
        {
            float sqr = v.SqrMagnitude;
            if (sqr > maxLength * maxLength)
            {
                float mag = GameMath.Sqrt(sqr);
                return new Vec3(v.x / mag * maxLength, v.y / mag * maxLength, v.z / mag * maxLength);
            }
            return v;
        }

        public static float Dot(Vec3 a, Vec3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x
        );

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;
        public static float SqrDistance(Vec3 a, Vec3 b) => (a - b).SqrMagnitude;

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            t = GameMath.Clamp01(t);
            return new Vec3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vec3 LerpUnclamped(Vec3 a, Vec3 b, float t)
        {
            return new Vec3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vec3 MoveTowards(Vec3 current, Vec3 target, float maxDistanceDelta)
        {
            Vec3 diff = target - current;
            float dist = diff.Magnitude;
            if (dist <= maxDistanceDelta || dist < 1e-6f) return target;
            return current + diff / dist * maxDistanceDelta;
        }

        public static Vec3 Project(Vec3 vector, Vec3 onNormal)
        {
            float sqrMag = Dot(onNormal, onNormal);
            if (sqrMag < 1e-15f) return Zero;
            float dot = Dot(vector, onNormal);
            return onNormal * (dot / sqrMag);
        }

        public static Vec3 ProjectOnPlane(Vec3 vector, Vec3 planeNormal)
        {
            return vector - Project(vector, planeNormal);
        }

        public static Vec3 Reflect(Vec3 inDirection, Vec3 inNormal)
        {
            return inDirection - 2f * Dot(inDirection, inNormal) * inNormal;
        }

        public static float Angle(Vec3 from, Vec3 to)
        {
            float denominator = GameMath.Sqrt(from.SqrMagnitude * to.SqrMagnitude);
            if (denominator < 1e-15f) return 0f;
            float dot = GameMath.Clamp(Dot(from, to) / denominator, -1f, 1f);
            return GameMath.Acos(dot) * GameMath.Rad2Deg;
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.x, -a.y, -a.z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.x * s, a.y * s, a.z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.x * s, a.y * s, a.z * s);
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.x / s, a.y / s, a.z / s);
        public static bool operator ==(Vec3 a, Vec3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vec3 a, Vec3 b) => !(a == b);

        public bool Equals(Vec3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 10) ^ (z.GetHashCode() << 20);
        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
    }
}