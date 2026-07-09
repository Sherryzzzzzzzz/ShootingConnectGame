using ShootingGame.Shared.Math;

namespace ShootingGame.Shared.ECS
{
    /// <summary>
    /// 子弹组件：飞行子弹的状态。
    /// </summary>
    public struct BulletComponent
    {
        public int AttackId;
        public int OwnerId;
        public int OwnerTeamId;
        public Vec3 Direction;
        public float Speed;
        public float MaxDistance;
        public float TraveledDistance;
        public int Damage;
        public int SpawnFrameId;

        public bool HasExceededMaxDistance => TraveledDistance >= MaxDistance;
    }
}
