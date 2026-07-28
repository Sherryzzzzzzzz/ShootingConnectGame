namespace ShootingGame.Shared.Physics
{
    /// <summary>
    /// 物理相关常量。与 Simulation.GameConstants 独立，避免 Physics → Simulation 循环依赖。
    /// </summary>
    public static class PhysicsConstants
    {
        // 玩家碰撞体
        public const float PlayerHeight = 1.8f;
        public const float PlayerRadius = 0.35f;
        public const float HitCapsuleRadius = 0.5f;
        public const float BulletRadius = 0.1f;
        public const float FootCapsuleOffset = 0.2f;

        // 移动
        public const float Gravity = -20f;
        public const float SlopeLimit = 45f;
        public const float MaxStepHeight = 0.3f;
    }
}
