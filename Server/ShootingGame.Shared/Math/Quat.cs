using System;

namespace ShootingGame.Shared.Math
{
    public struct Quat : IEquatable<Quat>
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quat(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static readonly Quat Identity = new Quat(0, 0, 0, 1);

        public float SqrMagnitude => x * x + y * y + z * z + w * w;

        public Quat Normalized
        {
            get
            {
                float mag = GameMath.Sqrt(SqrMagnitude);
                if (mag < 1e-6f) return Identity;
                return new Quat(x / mag, y / mag, z / mag, w / mag);
            }
        }

        public Quat Conjugate => new Quat(-x, -y, -z, w);

        /// <summary>
        /// Create a quaternion from Euler angles (degrees). Order: Y (yaw) → X (pitch) → Z (roll).
        /// </summary>
        public static Quat Euler(float pitch, float yaw, float roll)
        {
            float halfX = pitch * GameMath.Deg2Rad * 0.5f;
            float halfY = yaw * GameMath.Deg2Rad * 0.5f;
            float halfZ = roll * GameMath.Deg2Rad * 0.5f;

            float sx = GameMath.Sin(halfX), cx = GameMath.Cos(halfX);
            float sy = GameMath.Sin(halfY), cy = GameMath.Cos(halfY);
            float sz = GameMath.Sin(halfZ), cz = GameMath.Cos(halfZ);

            return new Quat(
                cy * sx * cz + sy * cx * sz,
                sy * cx * cz - cy * sx * sz,
                cy * cx * sz - sy * sx * cz,
                cy * cx * cz + sy * sx * sz
            );
        }

        public Vec3 EulerAngles
        {
            get
            {
                // pitch (x)
                float sinp = 2f * (w * x + y * z);
                float cosp = 1f - 2f * (x * x + y * y);
                float pitch = GameMath.Atan2(sinp, cosp) * GameMath.Rad2Deg;

                // yaw (y) — 使用 Atan2 支持全范围 [-180°, 180°]
                float siny = 2f * (w * y + x * z);
                float cosy = 1f - 2f * (y * y + x * x);
                float yaw = GameMath.Atan2(siny, cosy) * GameMath.Rad2Deg;

                // roll (z)
                float sinr = 2f * (w * z + x * y);
                float cosr = 1f - 2f * (y * y + z * z);
                float roll = GameMath.Atan2(sinr, cosr) * GameMath.Rad2Deg;

                return new Vec3(pitch, yaw, roll);
            }
        }

        /// <summary>
        /// Rotate a vector by this quaternion.
        /// </summary>
        public Vec3 Rotate(Vec3 v)
        {
            // q * v * q^-1, optimized
            float tx = 2f * (y * v.z - z * v.y);
            float ty = 2f * (z * v.x - x * v.z);
            float tz = 2f * (x * v.y - y * v.x);
            return new Vec3(
                v.x + w * tx + (y * tz - z * ty),
                v.y + w * ty + (z * tx - x * tz),
                v.z + w * tz + (x * ty - y * tx)
            );
        }

        public static Vec3 operator *(Quat q, Vec3 v) => q.Rotate(v);

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z
        );

        public static float Dot(Quat a, Quat b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        public static Quat Slerp(Quat a, Quat b, float t)
        {
            t = GameMath.Clamp01(t);
            float dot = Dot(a, b);

            // Ensure shortest path
            if (dot < 0f)
            {
                b = new Quat(-b.x, -b.y, -b.z, -b.w);
                dot = -dot;
            }

            if (dot > 0.9995f)
            {
                // Linearly interpolate for very close quaternions
                var result = new Quat(
                    a.x + (b.x - a.x) * t,
                    a.y + (b.y - a.y) * t,
                    a.z + (b.z - a.z) * t,
                    a.w + (b.w - a.w) * t
                );
                return result.Normalized;
            }

            float theta0 = GameMath.Acos(dot);
            float theta = theta0 * t;
            float sinTheta = GameMath.Sin(theta);
            float sinTheta0 = GameMath.Sin(theta0);

            float s0 = GameMath.Cos(theta) - dot * sinTheta / sinTheta0;
            float s1 = sinTheta / sinTheta0;

            return new Quat(
                s0 * a.x + s1 * b.x,
                s0 * a.y + s1 * b.y,
                s0 * a.z + s1 * b.z,
                s0 * a.w + s1 * b.w
            );
        }

        public static Quat RotateTowards(Quat from, Quat to, float maxDegreesDelta)
        {
            float dot = Dot(from, to);
            if (dot < 0f)
            {
                to = new Quat(-to.x, -to.y, -to.z, -to.w);
                dot = -dot;
            }
            if (dot > 0.9995f) return to;

            float angle = GameMath.Acos(GameMath.Clamp(dot, -1f, 1f)) * 2f * GameMath.Rad2Deg;
            if (angle < 1e-6f) return to;
            float t = GameMath.Min(1f, maxDegreesDelta / angle);
            return Slerp(from, to, t);
        }

        /// <summary>
        /// Build a rotation looking in the given forward direction, with the given up hint.
        /// </summary>
        public static Quat LookRotation(Vec3 forward, Vec3 up)
        {
            forward = forward.Normalized;
            if (forward.SqrMagnitude < 1e-6f) return Identity;

            Vec3 right = Vec3.Cross(up, forward).Normalized;
            if (right.SqrMagnitude < 1e-6f)
            {
                // forward is parallel to up, pick an arbitrary perpendicular
                right = Vec3.Cross(Vec3.Right, forward).Normalized;
                if (right.SqrMagnitude < 1e-6f)
                    right = Vec3.Cross(Vec3.Forward, forward).Normalized;
            }
            up = Vec3.Cross(forward, right);

            // Build rotation matrix then convert to quaternion
            float m00 = right.x, m01 = up.x, m02 = forward.x;
            float m10 = right.y, m11 = up.y, m12 = forward.y;
            float m20 = right.z, m21 = up.z, m22 = forward.z;

            float trace = m00 + m11 + m22;
            Quat q;
            if (trace > 0f)
            {
                float s = GameMath.Sqrt(trace + 1f) * 2f;
                q = new Quat((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = GameMath.Sqrt(1f + m00 - m11 - m22) * 2f;
                q = new Quat(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                float s = GameMath.Sqrt(1f + m11 - m00 - m22) * 2f;
                q = new Quat((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                float s = GameMath.Sqrt(1f + m22 - m00 - m11) * 2f;
                q = new Quat((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
            return q.Normalized;
        }

        public static Quat LookRotation(Vec3 forward) => LookRotation(forward, Vec3.Up);

        /// <summary>
        /// Create a rotation around a given axis (degrees).
        /// </summary>
        public static Quat AngleAxis(float angleDeg, Vec3 axis)
        {
            axis = axis.Normalized;
            float halfRad = angleDeg * GameMath.Deg2Rad * 0.5f;
            float s = GameMath.Sin(halfRad);
            return new Quat(axis.x * s, axis.y * s, axis.z * s, GameMath.Cos(halfRad));
        }

        public static bool operator ==(Quat a, Quat b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Quat a, Quat b) => !(a == b);

        public bool Equals(Quat other) => x == other.x && y == other.y && z == other.z && w == other.w;
        public override bool Equals(object obj) => obj is Quat q && Equals(q);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 8) ^ (z.GetHashCode() << 16) ^ (w.GetHashCode() << 24);
        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3}, {w:F3})";
    }
}
