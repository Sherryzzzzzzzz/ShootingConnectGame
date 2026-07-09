// 轴对齐包围盒
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
        /// 在每一边扩展AABB
        /// </summary>
        public AABB Expand(Vec3 amount)
        {
            return new AABB(Min - amount, Max + amount);
        }

        /// <summary>
        /// 按标量半径均匀扩展AABB
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

        /// <summary>
        /// 返回 AABB 上距离指定点最近的点。
        /// </summary>
        public Vec3 ClosestPoint(Vec3 point)
        {
            return new Vec3(
                Clamp(point.x, Min.x, Max.x),
                Clamp(point.y, Min.y, Max.y),
                Clamp(point.z, Min.z, Max.z)
            );
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public override string ToString() => $"AABB({Min}, {Max})";
    }
}