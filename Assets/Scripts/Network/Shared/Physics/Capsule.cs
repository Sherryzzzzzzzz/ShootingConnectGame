// 胶囊体碰撞器
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.Physics
{
    /// <summary>
    /// 由底部位置、高度和半径定义的胶囊碰撞器
    /// </summary>
    public struct Capsule
    {
        public Vec3 Base;    // 脚部位置
        public float Height;
        public float Radius;

        public Capsule(Vec3 basePos, float height, float radius)
        {
            Base = basePos;
            Height = height;
            Radius = radius;
        }

        public Vec3 Center => Base + Vec3.Up * (Height * 0.5f);

        /// <summary>
        /// 底部球心（脚部 + 半径向上）
        /// </summary>
        public Vec3 Bottom => Base + Vec3.Up * Radius;

        /// <summary>
        /// 顶部球心（底部 + 高度 - 半径向上）
        /// </summary>
        public Vec3 Top => Base + Vec3.Up * (Height - Radius);

        /// <summary>
        /// 构建完全包含此胶囊的AABB
        /// </summary>
        public AABB BoundingBox()
        {
            return new AABB(
                new Vec3(Base.x - Radius, Base.y, Base.z - Radius),
                new Vec3(Base.x + Radius, Base.y + Height, Base.z + Radius)
            );
        }

        /// <summary>
        /// 在给定脚部位置创建玩家大小的胶囊
        /// </summary>
        public static Capsule Player(Vec3 feetPosition)
        {
            return new Capsule(feetPosition, PhysicsConstants.PlayerHeight, PhysicsConstants.PlayerRadius);
        }
    }
}