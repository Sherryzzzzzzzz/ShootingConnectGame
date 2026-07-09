using ShootingGame.Network;
using ShootingGame.Shared.ECS.Components;
using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 变换组件：世界空间位置和旋转。
    /// </summary>
    [SyncComponent]
    public partial struct TransformComponent
    {
        [SyncVar] public Vec3 Position;
        [SyncVar] public Quat Rotation;

        /// <summary>Dirty tracker for network incremental sync.</summary>
        public DirtyTracker Dirty;

        public TransformComponent(Vec3 position, Quat rotation) : this()
        {
            Position = position;
            Rotation = rotation;
        }
    }
}
