using ShootingGame.Shared.Math;
using Xunit;

namespace ShootingGame.Tests
{
    public class Vec3Tests
    {
        const float E = 0.001f;

        [Fact]
        public void BasicArithmetic()
        {
            var a = new Vec3(1, 2, 3);
            var b = new Vec3(4, 5, 6);
            var sum = a + b;
            Assert.InRange(sum.x, 4.99f, 5.01f);
            Assert.InRange(sum.y, 6.99f, 7.01f);
            Assert.InRange(sum.z, 8.99f, 9.01f);

            var diff = b - a;
            Assert.InRange(diff.x, 2.99f, 3.01f);
        }

        [Fact]
        public void Magnitude()
        {
            var v = new Vec3(3, 4, 0);
            Assert.InRange(v.Magnitude, 5f - E, 5f + E);
            Assert.InRange(v.SqrMagnitude, 24.99f, 25.01f);
        }

        [Fact]
        public void Normalized()
        {
            var v = new Vec3(0, 0, 5);
            var n = v.Normalized;
            Assert.InRange(n.z, 1f - E, 1f + E);
            Assert.InRange(n.Magnitude, 1f - E, 1f + E);

            // Zero vector normalizes to zero
            var z = Vec3.Zero.Normalized;
            Assert.InRange(z.Magnitude, -E, E);
        }

        [Fact]
        public void DotProduct()
        {
            var a = new Vec3(1, 0, 0);
            var b = new Vec3(0, 1, 0);
            Assert.InRange(Vec3.Dot(a, b), -E, E); // perpendicular

            Assert.InRange(Vec3.Dot(a, a), 1f - E, 1f + E); // parallel
        }

        [Fact]
        public void CrossProduct()
        {
            var x = Vec3.Right;
            var y = Vec3.Up;
            var result = Vec3.Cross(x, y); // should be Forward (0,0,1)
            Assert.InRange(result.z, 1f - E, 1f + E);
            Assert.InRange(result.x, -E, E);
            Assert.InRange(result.y, -E, E);
        }

        [Fact]
        public void Distance()
        {
            var a = new Vec3(0, 0, 0);
            var b = new Vec3(3, 4, 0);
            Assert.InRange(Vec3.Distance(a, b), 5f - E, 5f + E);
        }

        [Fact]
        public void Lerp()
        {
            var a = Vec3.Zero;
            var b = new Vec3(10, 0, 0);
            var mid = Vec3.Lerp(a, b, 0.5f);
            Assert.InRange(mid.x, 5f - E, 5f + E);

            // Clamps t
            var over = Vec3.Lerp(a, b, 2f);
            Assert.InRange(over.x, 10f - E, 10f + E);
        }

        [Fact]
        public void ProjectOnPlane()
        {
            var v = new Vec3(1, 1, 0);
            var normal = Vec3.Up;
            var projected = Vec3.ProjectOnPlane(v, normal);
            Assert.InRange(projected.x, 1f - E, 1f + E);
            Assert.InRange(projected.y, -E, E);
        }

        [Fact]
        public void Angle()
        {
            var a = Vec3.Forward;
            var b = Vec3.Right;
            Assert.InRange(Vec3.Angle(a, b), 90f - 0.1f, 90f + 0.1f);

            Assert.InRange(Vec3.Angle(a, a), -0.1f, 0.1f);
        }

        [Fact]
        public void ClampMagnitude()
        {
            var v = new Vec3(10, 0, 0);
            var clamped = Vec3.ClampMagnitude(v, 3f);
            Assert.InRange(clamped.Magnitude, 3f - E, 3f + E);

            // Already under limit
            var small = new Vec3(1, 0, 0);
            var same = Vec3.ClampMagnitude(small, 3f);
            Assert.InRange(same.x, 1f - E, 1f + E);
        }
    }

    public class Vec2Tests
    {
        const float E = 0.001f;

        [Fact]
        public void BasicOps()
        {
            var a = new Vec2(1, 2);
            var b = new Vec2(3, 4);
            var sum = a + b;
            Assert.InRange(sum.x, 3.99f, 4.01f);
            Assert.InRange(sum.y, 5.99f, 6.01f);
        }

        [Fact]
        public void Magnitude()
        {
            var v = new Vec2(3, 4);
            Assert.InRange(v.Magnitude, 5f - E, 5f + E);
        }

        [Fact]
        public void Dot()
        {
            var a = new Vec2(1, 0);
            var b = new Vec2(0, 1);
            Assert.InRange(Vec2.Dot(a, b), -E, E);
        }
    }

    public class QuatTests
    {
        const float E = 0.01f;

        [Fact]
        public void Identity_DoesNotRotate()
        {
            var v = new Vec3(1, 2, 3);
            var rotated = Quat.Identity * v;
            Assert.InRange(rotated.x, v.x - E, v.x + E);
            Assert.InRange(rotated.y, v.y - E, v.y + E);
            Assert.InRange(rotated.z, v.z - E, v.z + E);
        }

        [Fact]
        public void Euler_90DegY_RotatesForwardToRight()
        {
            // Rotating (0,0,1) by 90 degrees around Y should give (1,0,0)
            var q = Quat.Euler(0, 90, 0);
            var result = q * Vec3.Forward;
            Assert.InRange(result.x, 1f - E, 1f + E);
            Assert.InRange(result.y, -E, E);
            Assert.InRange(result.z, -E, E);
        }

        [Fact]
        public void LookRotation_Forward()
        {
            var q = Quat.LookRotation(Vec3.Forward);
            var rotated = q * Vec3.Forward;
            Assert.InRange(rotated.z, 1f - E, 1f + E);
        }

        [Fact]
        public void LookRotation_Right()
        {
            var q = Quat.LookRotation(Vec3.Right);
            var result = q * Vec3.Forward; // should now point Right
            Assert.InRange(result.x, 1f - E, 1f + E);
            Assert.InRange(result.y, -E, E);
            Assert.InRange(result.z, -E, E);
        }

        [Fact]
        public void Slerp_Halfway()
        {
            var a = Quat.Identity;
            var b = Quat.Euler(0, 90, 0);
            var mid = Quat.Slerp(a, b, 0.5f);
            var result = mid * Vec3.Forward;
            // Should be roughly 45 degrees: (sin45, 0, cos45)
            float expected = GameMath.Sin(45f * GameMath.Deg2Rad);
            Assert.InRange(result.x, expected - E, expected + E);
        }

        [Fact]
        public void AngleAxis_90Y()
        {
            var q = Quat.AngleAxis(90, Vec3.Up);
            var result = q * Vec3.Forward;
            Assert.InRange(result.x, 1f - E, 1f + E);
            Assert.InRange(result.z, -E, E);
        }

        [Fact]
        public void Multiply_TwoRotations()
        {
            var a = Quat.Euler(0, 90, 0);
            var b = Quat.Euler(0, 90, 0);
            var combined = a * b; // should be 180 degrees around Y
            var result = combined * Vec3.Forward;
            Assert.InRange(result.z, -1f - E, -1f + E); // forward becomes back
        }

        [Fact]
        public void RotateTowards()
        {
            var from = Quat.Identity;
            var to = Quat.Euler(0, 90, 0);
            var result = Quat.RotateTowards(from, to, 45f);
            var forward = result * Vec3.Forward;
            // Should be 45 degrees rotated
            float cos45 = GameMath.Cos(45f * GameMath.Deg2Rad);
            float sin45 = GameMath.Sin(45f * GameMath.Deg2Rad);
            Assert.InRange(forward.x, sin45 - E, sin45 + E);
            Assert.InRange(forward.z, cos45 - E, cos45 + E);
        }
    }

    public class GameMathTests
    {
        const float E = 0.001f;

        [Fact]
        public void Clamp()
        {
            Assert.Equal(5f, GameMath.Clamp(10f, 0f, 5f));
            Assert.Equal(0f, GameMath.Clamp(-1f, 0f, 5f));
            Assert.Equal(3f, GameMath.Clamp(3f, 0f, 5f));
        }

        [Fact]
        public void Lerp()
        {
            Assert.InRange(GameMath.Lerp(0, 10, 0.5f), 5f - E, 5f + E);
            Assert.InRange(GameMath.Lerp(0, 10, 0f), -E, E);
            Assert.InRange(GameMath.Lerp(0, 10, 1f), 10f - E, 10f + E);
        }

        [Fact]
        public void InverseLerp()
        {
            Assert.InRange(GameMath.InverseLerp(0, 10, 5), 0.5f - E, 0.5f + E);
        }

        [Fact]
        public void DeltaAngle()
        {
            Assert.InRange(GameMath.DeltaAngle(10, 20), 10f - E, 10f + E);
            Assert.InRange(GameMath.DeltaAngle(350, 10), 20f - E, 20f + E);
            Assert.InRange(GameMath.DeltaAngle(10, 350), -20f - E, -20f + E);
        }
    }
}
