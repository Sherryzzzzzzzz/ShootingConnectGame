// 射线
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    public struct Ray
    {
        public Vec3 Origin;
        public Vec3 Direction;

        public Ray(Vec3 origin, Vec3 direction)
        {
            Origin = origin;
            Direction = direction.Normalized;
        }

        public Vec3 GetPoint(float distance) => Origin + Direction * distance;
    }
}