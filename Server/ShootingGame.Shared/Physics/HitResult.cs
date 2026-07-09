using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    public struct HitResult
    {
        public bool Hit;
        public float Distance;
        public Vec3 Point;
        public Vec3 Normal;

        public static readonly HitResult None = new HitResult { Hit = false };
    }
}
