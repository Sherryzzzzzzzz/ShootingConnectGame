using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    public struct AABB
    {
        public Vec3 Min;
        public Vec3 Max;

        public AABB(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        public Vec3 Center => (Min + Max) * 0.5f;
        public Vec3 Size => Max - Min;
        public Vec3 Extents => (Max - Min) * 0.5f;

        /// <summary>
        /// Expand the AABB by the given amount on each side.
        /// </summary>
        public AABB Expand(Vec3 amount)
        {
            return new AABB(Min - amount, Max + amount);
        }

        /// <summary>
        /// Expand the AABB uniformly by a scalar radius.
        /// </summary>
        public AABB Expand(float radius)
        {
            var r = new Vec3(radius, radius, radius);
            return new AABB(Min - r, Max + r);
        }

        public bool Contains(Vec3 point)
        {
            return point.x >= Min.x && point.x <= Max.x &&
                   point.y >= Min.y && point.y <= Max.y &&
                   point.z >= Min.z && point.z <= Max.z;
        }

        public bool Overlaps(AABB other)
        {
            return Min.x <= other.Max.x && Max.x >= other.Min.x &&
                   Min.y <= other.Max.y && Max.y >= other.Min.y &&
                   Min.z <= other.Max.z && Max.z >= other.Min.z;
        }

        public override string ToString() => $"AABB({Min}, {Max})";
    }
}
