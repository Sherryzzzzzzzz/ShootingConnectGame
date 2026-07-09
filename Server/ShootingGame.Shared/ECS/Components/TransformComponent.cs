using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 变换组件：世界空间位置和旋转。
    /// </summary>
    public struct TransformComponent
    {
        public Vec3 Position;
        public Quat Rotation;

        public TransformComponent(Vec3 position, Quat rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }
}
